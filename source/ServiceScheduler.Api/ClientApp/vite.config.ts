import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// API target for the dev proxy. Override with VITE_API_PROXY; defaults to the
// https profile in Properties/launchSettings.json.
const target =
  process.env['services__api__https__0'] ||
  process.env['services__api__http__0'] ||
  process.env['VITE_API_PROXY'] ||
  'https://localhost:7443';

const proxyOptions = { target, secure: false, changeOrigin: true };

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': proxyOptions,
      '/openapi': proxyOptions,
      '/health': proxyOptions,
    },
  },
  build: {
    outDir: 'build',
  },
});
