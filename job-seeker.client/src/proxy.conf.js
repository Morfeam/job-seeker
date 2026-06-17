const PROXY_CONFIG = [
  {
    context: ['/customer'],
    target: 'https://localhost:7107',
    secure: false
  },
  {
    context: ['/job-seeker'],
    target: 'https://localhost:7107',
    secure: false
  }
];

module.exports = PROXY_CONFIG;
