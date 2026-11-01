import http from 'k6/http';
import { check, sleep } from 'k6';

const baseUrl = (__ENV.BASE_URL || 'http://localhost:8107').replace(/\/$/, '');
const accessToken = __ENV.ACCESS_TOKEN;
const stage = Number(__ENV.STAGE || 1);

if (!accessToken) {
  throw new Error('ACCESS_TOKEN environment variable is required.');
}

export const options = {
  scenarios: {
    concurrent_game_results: {
      executor: 'constant-vus',
      vus: 50,
      duration: '1m',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<1000'],
    checks: ['rate>0.99'],
  },
};

export default function () {
  const achievementLevel = __ITER % 3;
  const response = http.post(
    `${baseUrl}/api/game/result`,
    JSON.stringify({ stage, achievementLevel }),
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        'Content-Type': 'application/json',
      },
      tags: { endpoint: 'game-result' },
    },
  );

  check(response, {
    'game result is saved': (result) => result.status === 200,
  });
  sleep(1);
}
