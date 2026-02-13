/** @type {import('next').NextConfig} */
const nextConfig = {
    output: 'export',
    images: {
        unoptimized: true,
    },
    // Electron環境では相対パスでアセットをロード
    assetPrefix: './',
    trailingSlash: true,
};

module.exports = nextConfig;
