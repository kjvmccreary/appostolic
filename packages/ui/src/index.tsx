import * as React from 'react';
import { Platform, Text as RNText, View as RNView } from 'react-native';

export const View: React.FC<React.PropsWithChildren> = ({ children }) => {
  return <RNView style={{ padding: 8 }}>{children}</RNView>;
};

export const Text: React.FC<React.PropsWithChildren<{ bold?: boolean }>> = ({
  children,
  bold,
}) => {
  return (
    <RNText style={{ fontWeight: bold ? '700' : '400' }}>
      {children}
      {Platform.OS === 'web' ? null : null}
    </RNText>
  );
};
