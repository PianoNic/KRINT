import {
  signalStore,
  withHooks,
  withMethods,
  withState,
  withComputed
} from '@ngrx/signals';

type TestState = {

};

export const initialTestStore: TestState = {

};
 
 export const TestStore = signalStore(
	{ providedIn: 'root' },
	withState(initialTestStore),
	withComputed((store) => ({})),
	withMethods((store) => ({})),
	withHooks((store) => ({}))
 );
 