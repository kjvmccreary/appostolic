import React from 'react';
// Using plain <a> to avoid a transient @types/react mismatch with next/link in monorepo
import { View, Text } from '@appostolic/ui';

export default function Page() {
  return (
    <main className="p-24">
      <h1>appostolic web</h1>
      <View>
        <Text bold>Shared UI works on web</Text>
      </View>
      <p>
        <a href="/dev">Dev page</a>
      </p>
    </main>
  );
}
