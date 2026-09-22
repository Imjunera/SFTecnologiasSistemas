
export interface ApiResponse<T> {
  status: number;
  data: T | null;
  success: boolean;
  error?: string;
  message?: string;
  details?: any;
}

// You can also define specific error types if needed.
export interface ErrorResponse {
  message: string;
  // other fields as needed
}

