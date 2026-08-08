/**
 * Compose fragment a user's own service needs in order to reach a KRINT-provisioned container by
 * name. KRINT attaches every container it provisions to its own Docker network, so joining that
 * network as external is what makes the container name resolve.
 *
 * This is the only route to a node-hosted instance: its published port is bound to the node's
 * loopback, so no address on the node's host is reachable from another container.
 *
 * When the network name is unknown (the inspect failed, or the node is offline) we emit a
 * placeholder rather than guessing, because a wrong name fails at container start with a message
 * that points nowhere near the cause.
 */
export function dockerNetworkComposeSnippet(dockerNetwork?: string | null): string {
  return [
    'services:',
    '  your-app:',
    '    networks: [krint]',
    '',
    'networks:',
    '  krint:',
    '    external: true',
    `    name: ${dockerNetwork ?? '<your krint network>'}`,
  ].join('\n');
}
