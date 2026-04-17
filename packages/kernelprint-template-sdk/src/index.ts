export type KernelPrintDataListener<T = unknown> = (data: T) => void | Promise<void>;

/**
 * Marks the template as ready for KernelPrint PDF generation.
 * Set this after your layout/fonts are stable.
 */
export function markKernelPrintReady(): void {
  (window as unknown as { __KERNELPRINT_READY__?: boolean }).__KERNELPRINT_READY__ = true;
}

/**
 * Listens for KernelPrint injected data (`kernelprint:data-ready`).
 * Returns an unsubscribe function.
 */
export function onKernelPrintDataReady<T = unknown>(listener: KernelPrintDataListener<T>): () => void {
  const handler = (event: Event) => {
    const custom = event as CustomEvent<T>;
    void listener(custom.detail);
  };

  window.addEventListener("kernelprint:data-ready", handler as EventListener);
  return () => window.removeEventListener("kernelprint:data-ready", handler as EventListener);
}

/**
 * Reads injected data synchronously (may be undefined before the event fires).
 */
export function readKernelPrintData<T = unknown>(): T | undefined {
  return (window as unknown as { __KERNELPRINT_DATA__?: T }).__KERNELPRINT_DATA__;
}
