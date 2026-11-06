/** @type {import('next').NextConfig} */
const nextConfig = {
  output: 'export',
  distDir: '../backend/wwwroot',
  images: {
    unoptimized: true,
  },
};

export default nextConfig;

