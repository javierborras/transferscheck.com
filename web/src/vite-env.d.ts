interface ImportMetaEnv {
  readonly DEV: boolean;
  readonly VITE_API_ORIGIN?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
