export interface AppError {
  status: number;
  message: string;
  validationErrors?: Record<string, string[]>;
}
