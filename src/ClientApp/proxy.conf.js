// Dev-server proxy for /api and /health.
//
// The API target is env-driven so one config works everywhere: on the host `ng serve` reaches the
// API at localhost:5080 (the default), and in the docker-compose client container it reaches it by
// the compose service name via API_PROXY_TARGET=http://api:5080.
const target = process.env['API_PROXY_TARGET'] || 'http://localhost:5080';

module.exports = {
  '/api': { target, secure: false, changeOrigin: true },
  '/health': { target, secure: false },
};
