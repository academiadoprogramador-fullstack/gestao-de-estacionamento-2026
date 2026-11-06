export interface LoginCredentials {
  email: string;
  password: string;
}

export interface EmployeeSession {
  userId: string;
  userName: string;
  roles: string[];
}

interface AntiforgeryToken {
  requestToken: string;
  headerName: string;
}

export type { AntiforgeryToken };
