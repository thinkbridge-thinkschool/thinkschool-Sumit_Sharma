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
