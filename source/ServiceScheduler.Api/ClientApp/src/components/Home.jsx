export function Home() {
  return (
    <div>
      <h1>Welcome</h1>
      <p>
        A full-stack starter with a <a href="https://react.dev/">React</a> frontend and an{' '}
        <a href="https://learn.microsoft.com/aspnet/core">ASP.NET Core</a> backend, built with:
      </p>
      <ul>
        <li><a href="https://learn.microsoft.com/aspnet/core">ASP.NET Core</a> minimal APIs with a lightweight in-process mediator</li>
        <li><a href="https://react.dev/">React</a> + <a href="https://vite.dev/">Vite</a> for the client</li>
        <li><a href="https://picocss.com/">Pico CSS</a> for styling and <a href="https://lucide.dev/">Lucide</a> for icons</li>
      </ul>
      <p>
        Open the <strong>Widgets</strong> page to create and list widgets via the API. The{' '}
        <code>ClientApp</code> folder is a Vite + React app &mdash; run <code>npm run dev</code> there for
        hot-reload during development.
      </p>
    </div>
  );
}
