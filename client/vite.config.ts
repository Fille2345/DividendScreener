import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Forward API calls to the .NET backend (http launch profile) during development.
    proxy: {
      '/api': 'http://localhost:5020',
    },
  },
})
