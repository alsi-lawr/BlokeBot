export async function copyText(value) {
  await navigator.clipboard.writeText(value);
}
