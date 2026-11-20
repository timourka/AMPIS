"""Run after docker compose up --build --wait. Restores stopped services."""
from contextlib import contextmanager
import os
from pathlib import Path
from subprocess import run
from uuid import uuid4

from smoke import account, call, eventually, upload, require_isolated


def compose(*args):
    run(['docker', 'compose', *args], cwd=Path(__file__).resolve().parent, check=True)


@contextmanager
def stopped(*services):
    compose('stop', *services)
    try:
        yield
    finally:
        compose('start', *services)


def main():
    require_isolated()
    token = account('Отказоустойчивость')
    other = account('Другой владелец')
    name = f'{uuid4().hex}.bin'
    contents = b'CQRS file'
    file_id = upload(contents, token, name=name)['id']
    download = f'/api/download/{file_id}'
    listing = f'/api/files?q={name}'
    my_files = '/api/my-files'
    downloads = '/api/downloads'
    item = {'id': file_id, 'name': name}
    assert eventually(lambda: call(download)) == contents

    def listed(total):
        assert call(listing)['total'] == total

    eventually(lambda: listed(1))
    with stopped('s2'):
        upload(b'check', token, status=503)
        call(my_files, token=token, status=503)
        assert item in call(downloads)
        assert call(download) == contents
        listed(1)
    with stopped('b2'):
        assert call(download) == contents
        listed(1)
    with stopped('s3'):
        call(download, status=503)
        call(downloads, status=503)
        assert item in call(my_files, token=token)
        listed(1)
        eventually(lambda: upload(b'while s3 offline', token))
    eventually(lambda: call(download))
    with stopped('s4'):
        call(listing, status=503)
        call('/api/statistics/' + file_id, token=token, status=503)
        call(my_files, status=401)
        assert call(my_files, token=other) == []
        selected = next(entry for entry in call(downloads) if entry == item)
        assert call('/api/download/' + selected['id']) == contents
        removable_id = upload(b'delete while s4 offline', token, name='removable.bin')['id']
        selected = next(entry for entry in call(my_files, token=token) if entry['id'] == removable_id)
        call('/api/files/' + selected['id'], method='DELETE', token=token, status=204)
        assert selected not in call(my_files, token=token)
        eventually(lambda: upload(b'while s4 offline', token))
    eventually(lambda: listed(1))
    with stopped('s1'):
        call('/api/login', body={'login': 'nobody', 'password': 'irrelevant'}, status=503)
        listed(1)
        assert call(download) == contents
    # Disconnect D1→S4 only. Publishers can still send into the durable queue.
    report_net = os.environ.get('COMPOSE_PROJECT_NAME', 'ampis-lab2') + '_reports'
    s4_id = run(['docker', 'compose', 'ps', '-q', 's4'], cwd=Path(__file__).resolve().parent,
                check=True, capture_output=True, text=True).stdout.strip()
    run(['docker', 'network', 'disconnect', report_net, s4_id], check=True)
    try:
        call(f'/api/files/{file_id}', method='DELETE', token=token, status=204)
        eventually(lambda: call(download, status=404))
        listed(1)
    finally:
        run(['docker', 'network', 'connect', report_net, s4_id], check=True)
    eventually(lambda: listed(0))
    print('PASS: S1–S4 stop scenarios and S4 catch-up after D1→S4 reconnect')


if __name__ == '__main__':
    main()
