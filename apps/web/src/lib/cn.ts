import classNames, { Argument } from 'classnames';

export function cn(...args: Argument[]): string {
  return classNames(...args);
}
