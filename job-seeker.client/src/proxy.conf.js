const PROXY_CONFIG = [
  {
    context: ['/customer'],
    target: 'https://localhost:7001',
    secure: false
  }
];

module.exports = PROXY_CONFIG;
