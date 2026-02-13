import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
    title: 'Sidecar MultiSender',
    description: 'Screen capture and relay sender for Sidecar',
};

export default function RootLayout({
    children,
}: {
    children: React.ReactNode;
}) {
    return (
        <html lang="ja">
            <body>{children}</body>
        </html>
    );
}
