#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

if [[ ! -f .env ]]; then
  echo ".env 파일이 없습니다. .env.example을 복사한 뒤 실제 값을 입력해주세요." >&2
  exit 1
fi

echo "[1/3] 이미지를 빌드합니다."
docker compose build --pull

echo "[2/3] 컨테이너를 갱신합니다."
docker compose up -d --remove-orphans

echo "[3/3] 애플리케이션 상태를 확인합니다."
for attempt in {1..30}; do
  if curl --fail --silent http://127.0.0.1:8107/actuator/health | grep -q '"status":"UP"'; then
    docker compose ps
    echo "배포가 완료되었습니다."
    exit 0
  fi
  sleep 2
done

docker compose logs --tail=100 app >&2
echo "애플리케이션이 제한 시간 안에 정상 상태가 되지 않았습니다." >&2
exit 1
