// API contract types. Mirror the DTOs in src/Api/Features/*.

export interface AuthResponse {
  accessToken: string;
}

export interface Usage {
  quotaBytes: number;
  usedBytes: number;
  remainingBytes: number;
}

export interface InitUploadResponse {
  mediaId: string;
  key: string;
  uploadId: string;
  partSize: number;
}

export interface PartUrlResponse {
  partNumber: number;
  url: string;
}

export interface CompletePart {
  partNumber: number;
  eTag: string;
}

export interface MediaListItem {
  id: string;
  originalName: string;
  contentType: string | null;
  sizeBytes: number;
  createdAt: string;
}

export interface DownloadUrlResponse {
  url: string;
}

/** Client-side view of an in-progress or finished upload. */
export interface UploadTask {
  id: string;
  fileName: string;
  totalBytes: number;
  uploadedBytes: number;
  status: 'uploading' | 'completing' | 'done' | 'error' | 'aborted';
  error?: string;
}
