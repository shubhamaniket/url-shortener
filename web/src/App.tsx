import { useState } from 'react';

import { CreateLinkForm } from './CreateLinkForm';
import { LinkLookup, type LookupRequest } from './LinkLookup';

export function App() {
  const [lookupRequest, setLookupRequest] = useState<LookupRequest>();

  return (
    <main>
      <h1>URL Shortener</h1>
      <p>Shorten a link, then see how it performs.</p>
      <CreateLinkForm onViewDetails={(code) => setLookupRequest({ code, id: Date.now() })} />
      <LinkLookup request={lookupRequest} />
    </main>
  );
}
