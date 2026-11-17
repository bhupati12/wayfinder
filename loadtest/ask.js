// k6 load test for POST /api/v1/ask. A local CPU model answers one question at a time, so this
// uses a small, fixed concurrency and a long timeout; the interesting numbers are latency and
// the cache's effect on repeated questions.
//   k6 run -e API_KEY=... loadtest/ask.js
import http from "k6/http";
import { check } from "k6";

const BASE_URL = __ENV.BASE_URL || "http://localhost:5099";
const API_KEY = __ENV.API_KEY;

const questions = [
  "What is Hanoi's most famous dish?",
  "How long can transit passengers stay in Shanghai without a visa?",
  "Which airport handles most international flights to Seoul?",
  "When is the Chiang Mai Flower Festival held?",
];

export const options = {
  scenarios: {
    ask: {
      executor: "constant-vus",
      vus: Number(__ENV.VUS || 2),
      duration: __ENV.DURATION || "5m",
    },
  },
  thresholds: {
    http_req_failed: ["rate<0.05"],
  },
  summaryTrendStats: ["avg", "med", "p(90)", "p(95)", "max"],
};

export default function () {
  const question = questions[(__ITER + __VU) % questions.length];
  const res = http.post(`${BASE_URL}/api/v1/ask`, JSON.stringify({ question }), {
    headers: { "Content-Type": "application/json", "X-Api-Key": API_KEY },
    timeout: "320s",
  });
  check(res, {
    "status 200": (r) => r.status === 200,
    "cached flag present": (r) => r.status !== 200 || r.json("metadata.cached") !== undefined,
  });
}
