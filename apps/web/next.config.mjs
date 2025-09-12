/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  experimental: {
    instrumentationHook: false,
  },
  eslint: {
    // ESLint v9 flat config can trigger incompatible option warnings in Next's embedded runner.
    // Keep linting in CI via pnpm scripts and skip during production builds.
    ignoreDuringBuilds: true,
  },
  transpilePackages: [
    '@appostolic/ui',
    '@appostolic/models',
    '@appostolic/sdk',
    'react-native',
    'react-native-web',
  ],
  webpack: (config) => {
    config.resolve.alias = {
      ...(config.resolve.alias || {}),
      'react-native$': 'react-native-web',
    };
    return config;
  },
};

export default nextConfig;
