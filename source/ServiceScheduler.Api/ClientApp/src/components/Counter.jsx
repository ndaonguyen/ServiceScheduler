import { useState } from 'react';

export function Counter() {
  const [count, setCount] = useState(0);

  return (
    <div>
      <h1>Counter</h1>
      <p>A simple example of a React component with local state.</p>
      <p aria-live="polite">Current count: <strong>{count}</strong></p>
      <button onClick={() => setCount(c => c + 1)}>Increment</button>
    </div>
  );
}
