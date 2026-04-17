/** ESLint flat config at repo root: ignore non-JS paths (Helm/YAML, .NET outputs). */
export default [
  {
    ignores: [
      'deploy/**',
      '**/bin/**',
      '**/obj/**',
      '**/node_modules/**',
      '**/dist/**',
      '**/*.dll',
      '**/*.pdb',
    ],
  },
]
