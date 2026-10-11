import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

// Testing Library only cleans up automatically when test globals are enabled; this project imports
// test functions explicitly, so unmount rendered components after every test here.
afterEach(cleanup);
