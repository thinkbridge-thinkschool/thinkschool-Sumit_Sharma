export interface Quote {
  id: number;
  author: string;
  text: string;
  isDeleted: boolean;
}

export interface QuoteRequest {
  author: string;
  text: string;
}

export interface QuoteValidationProblem {
  title?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
