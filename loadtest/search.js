// k6 load test for GET /api/v1/search.
//   k6 run loadtest/search.js                       (defaults: hybrid, ramp to 40 VUs)
//   k6 run -e MODE=keyword -e BASE_URL=http://localhost:5099 loadtest/search.js
// Ramps up in stages so the request rate at which errors or latency begin is visible.
import http from "k6/http";
import { check } from "k6";

const BASE_URL = __ENV.BASE_URL || "http://localhost:5099";
const MODE = __ENV.MODE || "hybrid";

const queries = [
  "where to eat ramen in Osaka",
  "how to get from the airport to Seoul",
  "temples in Kyoto",
  "is Bangkok safe at night",
  "visa for Singapore",
  "beaches in Bali",
  "street food in Hanoi",
  "Great Wall from Beijing",
  "night market in Taipei",
  "ferry to Boracay",
  "Angkor Wat tickets",
  "cheap hostels in Kuala Lumpur",
];

export const options = {
  scenarios: {
    ramp: {
      executor: "ramping-vus",
      startVUs: 1,
      stages: [
        { duration: "30s", target: 5 },
        { duration: "30s", target: 10 },
        { duration: "30s", target: 20 },
        { duration: "30s", target: 40 },
        { duration: "15s", target: 0 },
      ],
    },
  },
  thresholds: {
    http_req_failed: ["rate<0.01"],
    http_req_duration: ["p(95)<1000"],
  },
  summaryTrendStats: ["avg", "med", "p(90)", "p(95)", "p(99)", "max"],
};

export default function () {
  // Vary queries per iteration; with the cache on, repeats measure the cached path.
  const q = queries[(__ITER + __VU) % queries.length];
  const res = http.get(`${BASE_URL}/api/v1/search?q=${encodeURIComponent(q)}&mode=${MODE}&k=10`, {
    tags: { mode: MODE },
  });
  check(res, {
    "status 200": (r) => r.status === 200,
    "has results": (r) => r.status === 200 && r.json("results").length > 0,
  });
}
