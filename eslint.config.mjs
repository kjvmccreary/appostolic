// Flat ESLint config for the monorepo (ESLint v9)
import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import prettier from 'eslint-config-prettier';

export default [
  {
    ignores: [
      '**/node_modules/**',
      '**/.turbo/**',
      '**/dist/**',
      '**/build/**',
      '**/.next/**',
      '**/bin/**',
      '**/obj/**',
      '**/tailwind.config.*',
      '**/postcss.config.*',
    ],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['**/*.ts', '**/*.tsx'],
    languageOptions: {
      parserOptions: {
        tsconfigRootDir: import.meta.dirname,
        projectService: true,
      },
    },
    rules: {},
  },
  {
    // Allow CommonJS + Node globals in JS config files
    files: [
      '**/*.config.js',
      '**/*.config.cjs',
      'apps/mobile/metro.config.js',
      'apps/mobile/babel.config.js',
      'apps/web/postcss.config.js',
      'apps/web/tailwind.config.ts',
    ],
    languageOptions: {
      sourceType: 'commonjs',
      globals: {
        module: 'readonly',
        require: 'readonly',
        __dirname: 'readonly',
      },
      parserOptions: {
        // Avoid TypeScript project service on non-project config files
        projectService: false,
      },
    },
    rules: {
      '@typescript-eslint/no-require-imports': 'off',
      'no-undef': 'off',
    },
  },
  {
    files: ['apps/web/**/*.{ts,tsx}', 'packages/ui/**/*.{ts,tsx}'],
    languageOptions: {
      globals: {
        window: 'readonly',
        document: 'readonly',
      },
    },
  },
  prettier,
];
