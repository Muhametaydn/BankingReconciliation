import http from 'k6/http';
import { check } from 'k6';

const baseUrl = __ENV.BASE_URL || 'http://127.0.0.1:5097';
const branchCsv = `BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
BR001,FUND-A,TX001,2026-08-10,100.00,2500.00
BR001,FUND-A,TX002,2026-08-10,50.00,1250.00`;
const bankCsv = `BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
BR001,FUND-A,TX001,2026-08-10,100.00,2500.00
BR001,FUND-A,TX002,2026-08-10,50.00,1250.00`;

export const options = {
  scenarios: {
    reconciliation: {
      executor: 'constant-arrival-rate',
      rate: Number(__ENV.REQUESTS_PER_SECOND || 5),
      timeUnit: '1s',
      duration: __ENV.DURATION || '20s',
      preAllocatedVUs: 10,
      maxVUs: 30,
    },
  },
  thresholds: {
    checks: ['rate>0.99'],
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<1000', 'p(99)<2000'],
  },
};

export default function () {
  const response = http.post(`${baseUrl}/api/reconciliations/compare`, {
    branchFile: http.file(branchCsv, 'branch.csv', 'text/csv'),
    bankFile: http.file(bankCsv, 'bank.csv', 'text/csv'),
  });

  check(response, {
    'comparison returns 200': (result) => result.status === 200,
    'comparison completes as an exact match': (result) => {
      if (result.status !== 200) return false;
      const payload = result.json();
      return payload.batchStatus === 'Completed' &&
        payload.matchedCount === 2 &&
        payload.mismatchCount === 0 &&
        payload.onlyInBranchCount === 0 &&
        payload.onlyInBankCount === 0;
    },
  });
}
