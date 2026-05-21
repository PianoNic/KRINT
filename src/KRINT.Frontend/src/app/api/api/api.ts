export * from './app.service';
import { AppService } from './app.service';
export * from './database.service';
import { DatabaseService } from './database.service';
export const APIS = [AppService, DatabaseService];
