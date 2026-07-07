import { NavMenu } from './NavMenu';

export function Layout({ children }) {
  return (
    <>
      <NavMenu />
      <main className="container">{children}</main>
    </>
  );
}
