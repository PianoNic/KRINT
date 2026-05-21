export * from './app.service';
import { AppService } from './app.service';
export * from './test.service';
import { TestService } from './test.service';
export const APIS = [AppService, TestService];
