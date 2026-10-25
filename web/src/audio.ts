let context: AudioContext | undefined;
const listeners = new Set<() => void>();
export const soundEnabled = () => context?.state === "running";
export function subscribeSound(listener: () => void) {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}
export async function unlockSound() {
  context ??= new AudioContext();
  context.onstatechange = () => listeners.forEach(listener => listener());
  await context.resume();
  listeners.forEach(listener => listener());
  playCashBell();
}
export function playCashBell(delaySeconds = 0) {
  if (!context || context.state !== "running") return;
  const start = context.currentTime + delaySeconds;
  [523.25, 659.25, 783.99].forEach((frequency, index) => {
    const tone = context!.createOscillator();
    const gain = context!.createGain();
    const at = start + index * 0.12;
    tone.type = "sine";
    tone.frequency.value = frequency;
    gain.gain.setValueAtTime(0, at);
    gain.gain.linearRampToValueAtTime(0.16, at + 0.01);
    gain.gain.exponentialRampToValueAtTime(0.001, at + 0.65);
    tone.connect(gain);
    gain.connect(context!.destination);
    tone.start(at);
    tone.stop(at + 0.7);
    tone.onended = () => { tone.disconnect(); gain.disconnect(); };
  });
}
