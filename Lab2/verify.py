"""Run the lab tests in disposable containers and volumes, never in the user database."""
import os
import subprocess
import sys
from pathlib import Path
from uuid import uuid4

root = Path(__file__).resolve().parent
project = 'lab2-check-' + uuid4().hex[:8]
env = dict(os.environ, COMPOSE_PROJECT_NAME=project, LAB2_PORT='18088', LAB2_URL='http://127.0.0.1:18088')

def run(*args):
    subprocess.run(args, cwd=root, env=env, check=True)

try:
    run('docker', 'compose', 'up', '--build', '-d', '--wait')
    run(sys.executable, 'smoke.py')
    run(sys.executable, 'failures.py')
finally:
    run('docker', 'compose', 'down', '--volumes')
