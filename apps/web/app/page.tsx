import { getServerSession } from 'next-auth';
import { redirect } from 'next/navigation';
import { authOptions } from '../src/lib/auth';

export default async function RootPage() {
  const session = await getServerSession(authOptions).catch(() => null);
  // If not authenticated, go to login
  if (!session?.user?.email) {
    redirect('/login');
  }
  // If authenticated, enter the app; /studio further redirects to /studio/agents
  redirect('/studio');
}
