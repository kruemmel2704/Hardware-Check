import './globals.css';

export const viewport = {
  width: 'device-width',
  initialScale: 1,
  maximumScale: 5,
  themeColor: '#0f2b4d',
};

export const metadata = {
  title: 'CPM Hardware-Check | Libre Hardware Monitor (Port 8085)',
  description: 'CPM Hardware-Diagnose & Libre Hardware Monitor Port 8085 Dashboard',
  icons: {
    icon: '/favicon.png',
  },
  appleWebApp: {
    capable: true,
    statusBarStyle: 'default',
    title: 'CPM Hardware-Check',
  },
};

export default function RootLayout({ children }) {
  return (
    <html lang="de">
      <body>{children}</body>
    </html>
  );
}

