// Same limit as the server's ImageUpload.MaxBytes. Checked before uploading: a bigger file used to
// spin on "Saving..." until the request failed (QA run 3).
export const MAX_IMAGE_BYTES = 5 * 1024 * 1024

export function imageTooLarge(file: File) {
  return file.size > MAX_IMAGE_BYTES
}
