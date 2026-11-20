"""Integration test against docker compose up --build --wait. Python stdlib only."""
import json
import os
import secrets
import sys
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
from uuid import uuid4

BASE = os.environ.get('LAB2_URL', 'http://localhost:8088')

def call(path, *, body=None, method=None, token=None, content_type='application/json', status=200) -> Any:
    headers = {'Content-Type': content_type}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    if isinstance(body, dict):
        body = json.dumps(body).encode()
    try:
        with urlopen(Request(BASE + path, data=body, headers=headers, method=method), timeout=15) as response:
            actual, data = response.status, response.read()
    except HTTPError as error:
        actual, data = error.code, error.read()
    except URLError as error:
        raise AssertionError(f'{path}: приложение ещё не доступно: {error.reason}') from error
    assert actual == status, (path, actual, status, data[:300])
    if path.startswith('/api/') and not path.startswith('/api/download/') and data and actual < 300:
        return json.loads(data)
    return data

def eventually(test):
    from time import monotonic
    deadline = monotonic() + 20
    while True:
        try:
            return test()
        except AssertionError:
            if monotonic() >= deadline:
                raise

def upload(payload, token, *, status=201, name='file.bin'):
    boundary = uuid4().hex
    body = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\n'
            'Content-Type: application/octet-stream\r\n\r\n').encode() + payload + f'\r\n--{boundary}--\r\n'.encode()
    return call('/api/upload', body=body, token=token, content_type=f'multipart/form-data; boundary={boundary}', status=status)

def account(name):
    login = 'lab' + secrets.token_hex(5)
    password = 'lab-test-123!'
    call('/api/register', body={'login': login, 'password': password, 'name': name}, status=201)
    return call('/api/login', body={'login': login, 'password': password})['token']

def require_isolated():
    if not os.environ.get('COMPOSE_PROJECT_NAME', '').startswith('lab2-check-') or BASE != 'http://127.0.0.1:18088':
        raise SystemExit('Запустите python verify.py: тесты не должны писать в рабочую БД')


def main():
    call('/health')
    if '--health' in sys.argv:
        print('PASS: gateway health')
        return
    require_isolated()
    owner, visitor = account('Lab Owner'), account('Lab Visitor')
    blob = b'\0\xffbytes' * 149796 + b'\0' * 4
    assert len(blob) == 1048576
    upload(blob + b'x', owner, status=413)
    name = f'{uuid4().hex}.bin'
    file_id = upload(blob, owner, name=name)['id']
    def listed(expected_owner, count):
        result = call(f'/api/files?q={name}&sort=downloads&dir=desc&page=1&size=1')
        assert result['total'] == 1 and result['items'] == [dict(id=file_id,name=name,owner=expected_owner,downloads=count,isOwner=False)], result
        own = call(f'/api/files?q={name}', token=owner)['items'][0]
        other = call(f'/api/files?q={name}', token=visitor)['items'][0]
        assert own['isOwner'] is True and other['isOwner'] is False
    eventually(lambda: listed('Lab Owner', 0))
    assert eventually(lambda: call('/api/download/' + file_id)) == blob
    assert call('/api/download/' + file_id, token=visitor) == blob
    def stats():
        result = call('/api/statistics/' + file_id, token=owner)
        assert result == dict(total=2, anonymous=1, users=['Lab Visitor']), result
    eventually(stats)
    call('/api/statistics/' + file_id, token=visitor, status=403)
    call('/api/profile', body={'name': 'Lab Renamed'}, token=owner, status=204)
    eventually(lambda: listed('Lab Renamed', 2))
    call('/api/files/' + file_id, method='DELETE', token=visitor, status=403)
    call('/api/files/' + file_id, method='DELETE', token=owner, status=204)
    eventually(lambda: call('/api/download/' + file_id, status=404))
    print('PASS: registration, login, size limit, upload, replication, downloads, statistics, ownership, rename, deletion')

if __name__ == '__main__':
    main()
