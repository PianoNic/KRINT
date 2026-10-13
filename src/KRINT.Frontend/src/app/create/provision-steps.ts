// Turns the raw progress lines the provision hub streams ("Starting container krint-pg-1 on
// port 30001") into friendly timeline steps. The backend wording is matched loosely and anything
// unknown still shows up with its raw text, so a new server message never disappears.

export type ProvisionEvent = { readonly message: string; readonly at: number };

export type ProvisionPlan = {
  readonly plugins: number;
  readonly extraDatabases: number;
  readonly users: number;
};

export type StepState = 'pending' | 'active' | 'done' | 'failed';

type StepKey =
  | 'submit'
  | 'pull'
  | 'start'
  | 'wait'
  | 'defaultDb'
  | 'plugins'
  | 'setup'
  | 'fallback'
  | 'ready'
  | 'other';

export type ProvisionStep = {
  readonly id: string;
  readonly key: StepKey;
  readonly title: string;
  readonly detail: string | null;
  readonly state: StepState;
  readonly startedAt: number | null;
  readonly endedAt: number | null;
  readonly warnings: ReadonlyArray<string>;
};

type Classified = { key: StepKey; detail: string | null };

const RULES: ReadonlyArray<[RegExp, (m: RegExpMatchArray) => Classified]> = [
  [/^Submitting request/i, () => ({ key: 'submit', detail: null })],
  [/^Pulling image (\S+)/i, (m) => ({ key: 'pull', detail: m[1] })],
  [
    /^Starting container (\S+) on port (\d+)/i,
    (m) => ({ key: 'start', detail: `Container ${m[1]} on port ${m[2]}` }),
  ],
  [/^Waiting for \S+ to accept connections/i, () => ({ key: 'wait', detail: null })],
  [/^Creating default database (.+)$/i, (m) => ({ key: 'defaultDb', detail: m[1] })],
  [
    /^Enabling (\d+) plugin/i,
    (m) => ({ key: 'plugins', detail: m[1] === '1' ? '1 plugin' : `${m[1]} plugins` }),
  ],
  [/^Creating database (.+)$/i, (m) => ({ key: 'setup', detail: `Database ${m[1]}` })],
  [/^Creating user (.+)$/i, (m) => ({ key: 'setup', detail: `User ${m[1]}` })],
  [
    /^Live progress unavailable/i,
    () => ({
      key: 'fallback',
      detail: 'Live updates are not available on this connection. The result appears as soon as the server finishes.',
    }),
  ],
];

// The order the server walks through. Keys missing from a run (no image pull, no plugins) are
// simply skipped; it is only used to know which steps are still ahead.
const ORDER: ReadonlyArray<StepKey> = ['submit', 'pull', 'start', 'wait', 'defaultDb', 'plugins', 'setup', 'ready'];

function classify(message: string): Classified {
  for (const [re, map] of RULES) {
    const m = message.match(re);
    if (m) return map(m);
  }
  return { key: 'other', detail: null };
}

function titleFor(key: StepKey, engine: string, plan: ProvisionPlan, raw: string): string {
  switch (key) {
    case 'submit':
      return 'Sending your request';
    case 'pull':
      return `Downloading the ${engine} image`;
    case 'start':
      return 'Starting your database';
    case 'wait':
      return `Waiting for ${engine} to be ready`;
    case 'defaultDb':
      return 'Creating the default database';
    case 'plugins':
      return 'Enabling plugins';
    case 'setup':
      if (plan.users === 0) return 'Creating your databases';
      if (plan.extraDatabases === 0) return 'Creating your users';
      return 'Creating databases and users';
    case 'fallback':
      return 'Working on it';
    case 'ready':
      return 'All set';
    default:
      return raw;
  }
}

function expected(plan: ProvisionPlan): ReadonlySet<StepKey> {
  const keys = new Set<StepKey>(['start', 'wait', 'ready']);
  if (plan.plugins > 0) keys.add('plugins');
  if (plan.extraDatabases > 0 || plan.users > 0) keys.add('setup');
  return keys;
}

type Draft = {
  id: string;
  key: StepKey;
  title: string;
  detail: string | null;
  startedAt: number | null;
  endedAt: number | null;
  warnings: string[];
};

export function buildSteps(
  events: ReadonlyArray<ProvisionEvent>,
  engine: string,
  plan: ProvisionPlan,
  outcome: 'running' | 'failed' | 'done',
): ReadonlyArray<ProvisionStep> {
  const drafts: Draft[] = [];

  for (const ev of events) {
    const last = drafts.at(-1);
    const warning = ev.message.match(/^Warning:\s*(.*)$/is);
    if (warning) {
      // Partial-success notes belong to whatever step was running when they happened.
      if (last) last.warnings.push(warning[1]);
      continue;
    }

    const { key, detail } = classify(ev.message);
    if (last && key !== 'other' && last.key === key) {
      // A port retry repeats "Starting container", setup reports one line per database/user:
      // keep one row and show the newest detail.
      last.detail = detail;
      continue;
    }
    if (last) last.endedAt = ev.at;
    drafts.push({
      id: `${drafts.length}-${key}`,
      key,
      title: titleFor(key, engine, plan, ev.message),
      detail,
      startedAt: ev.at,
      endedAt: null,
      warnings: [],
    });
  }

  const reached = Math.max(-1, ...drafts.map((d) => ORDER.indexOf(d.key)));
  const seen = new Set(drafts.map((d) => d.key));
  // Without live updates the individual steps will never be reported; only the end is ahead.
  const ahead: ReadonlySet<StepKey> = seen.has('fallback') ? new Set(['ready']) : expected(plan);
  const pending: Draft[] = ORDER.filter((k, i) => i > reached && ahead.has(k) && !seen.has(k)).map((k) => ({
    id: `pending-${k}`,
    key: k,
    title: titleFor(k, engine, plan, ''),
    detail: null,
    startedAt: null,
    endedAt: null,
    warnings: [],
  }));

  const lastReal = drafts.length - 1;
  return [...drafts, ...pending].map((d, i): ProvisionStep => {
    let state: StepState;
    if (outcome === 'done') state = 'done';
    else if (i > lastReal) state = 'pending';
    else if (i < lastReal) state = 'done';
    else state = outcome === 'failed' ? 'failed' : 'active';
    return { ...d, state };
  });
}

// Engines that boot a JVM or run a heavy first-start init. Their readiness wait is routinely a
// minute or more, which looks like a hang without a word of explanation.
const SLOW_STARTERS: Readonly<Record<string, string>> = {
  cassandra: 'Cassandra runs on the JVM and usually needs a minute or two on its first start.',
  neo4j: 'Neo4j runs on the JVM and usually needs a minute or so on its first start.',
  clickhouse: 'ClickHouse initializes its data directory on first start, which can take up to a minute.',
  mssql: 'SQL Server sets up its system databases on first start, which usually takes a minute or so.',
  cockroachdb: 'CockroachDB initializes its cluster on first start, which can take a little while.',
};

export function slowHint(step: ProvisionStep | null, engineKey: string, now: number): string | null {
  if (!step || step.state !== 'active' || step.startedAt === null) return null;
  const elapsed = now - step.startedAt;
  if (step.key === 'pull' && elapsed > 10_000) {
    return 'The first download of an image can take a few minutes, depending on your connection. Later launches reuse it and start much faster.';
  }
  if (step.key === 'wait' && elapsed > 10_000 && SLOW_STARTERS[engineKey]) {
    return SLOW_STARTERS[engineKey];
  }
  if (elapsed > 45_000) {
    return 'This is taking longer than usual, but it is still working. Keep this page open until it finishes.';
  }
  return null;
}

// A short, human reading of the most common failures. The raw message stays available below it.
export function friendlyFailure(error: string): string {
  if (/password/i.test(error)) {
    return 'The password does not meet the rules for this engine. Change it, or leave the field blank to let KRINT generate one.';
  }
  if (/port/i.test(error) && /(free|available|allocated|range|in use)/i.test(error)) {
    return 'KRINT could not find a free port for this engine. Free one up, or widen its port range in krint.yaml.';
  }
  if (/(pull|manifest|image|registry)/i.test(error)) {
    return 'The container image could not be downloaded. Check that this machine can reach the image registry.';
  }
  if (/(accept connections|timed out|timeout|never became ready|not ready)/i.test(error)) {
    return 'The database started but did not become ready in time. The container log usually says why.';
  }
  if (/(already exists|duplicate|taken)/i.test(error)) {
    return 'Something with that name already exists. Pick a different name and try again.';
  }
  return 'Something went wrong while setting things up.';
}

// Short single-line server messages read fine on their own; long ones (container log tails,
// stack-like text) go behind a disclosure.
export function isShortError(error: string): boolean {
  return error.length <= 240 && !error.includes('\n');
}

export function formatElapsed(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  if (total < 60) return `${total}s`;
  const m = Math.floor(total / 60);
  const s = total % 60;
  return `${m}m ${s.toString().padStart(2, '0')}s`;
}
