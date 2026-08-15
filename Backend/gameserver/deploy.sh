#!/bin/bash

echo " [1/2] 최신 코드로 빌드 및 컨테이너 갱신 중..."

docker compose up --build -d

echo " [2/2] 컨테이너 상태를 확인합니다."
docker ps

echo "배포가 완료되었습니다."
