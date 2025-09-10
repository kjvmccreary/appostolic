/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  experimental: {
    instrumentationHook: false
  },
  transpilePackages: [
    '@appostolic/ui',
    '@appostolic/models',
    'react-native',
    'react-native-web'
  ],
  webpack: (config) => {
    config.resolve.alias = {
      ...(config.resolve.alias || {}),
      'react-native$': 'react-native-web',
    };
    return config;
  }
};

export default nextConfig;
