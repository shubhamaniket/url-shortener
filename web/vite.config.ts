import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The built page is served by the API itself (one origin): output goes to the API's web root.
// During `npm run dev`, API calls are proxied to the API running on its default port.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../src/UrlShortener.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5058',
      '/health': 'http://localhost:5058',
    },
  },
});
