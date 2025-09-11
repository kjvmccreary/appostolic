import React from 'react';
import Link from 'next/link';
import { View, Text } from '@appostolic/ui';

export default function Page() {
  return (
    <main className="p-24">
      <h1>appostolic web</h1>
      <View>
        <Text bold>Shared UI works on web</Text>
      </View>
      <p>
        <Link href="/dev">Dev page</Link>
      </p>
    </main>
  );
}
