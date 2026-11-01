/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The browser talks to one origin; the dev server routes each API prefix to the service that owns it.
const identity = process.env.IDENTITY_URL ?? 'http://localhost:5101';
const feedback = process.env.FEEDBACK_URL ?? 'http://localhost:5102';
const analytics = process.env.ANALYTICS_URL ?? 'http://localhost:5103';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api/auth': identity,
      '/api/feedback': feedback,
      '/api/stores': feedback,
      '/api/categories': feedback,
      '/api/analytics': analytics,
    },
  },
  test: { environment: 'node' },
});
