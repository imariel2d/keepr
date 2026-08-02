import { type APIRequestContext, expect } from '@playwright/test';
import { MAILPIT_URL } from './config';

// Thin client over Mailpit's HTTP API (https://mailpit.axllent.org/docs/api-v1/). Only the few
// endpoints journey A needs: empty the mailbox, search by recipient, and fetch a full message.

export interface MailpitSummary {
  ID: string;
  To: { Address: string }[];
  Subject: string;
}

export interface MailpitMessage {
  ID: string;
  Subject: string;
  Text: string;
  HTML: string;
  To: { Address: string }[];
  From: { Address: string };
}

/** Empties the mailbox so a "exactly one message" assertion is unambiguous. */
export async function clearMailbox(request: APIRequestContext): Promise<void> {
  const res = await request.delete(`${MAILPIT_URL}/api/v1/messages`);
  expect(res.ok(), `Mailpit delete-all failed (${res.status()})`).toBeTruthy();
}

/** Message summaries currently addressed to `to`. */
export async function messagesTo(request: APIRequestContext, to: string): Promise<MailpitSummary[]> {
  const res = await request.get(`${MAILPIT_URL}/api/v1/search`, { params: { query: `to:${to}` } });
  expect(res.ok(), `Mailpit search failed (${res.status()})`).toBeTruthy();
  const body = (await res.json()) as { messages?: MailpitSummary[] };
  return body.messages ?? [];
}

/** Polls until exactly one message is addressed to `to`, then returns its full content. */
export async function waitForSingleMessageTo(
  request: APIRequestContext,
  to: string,
): Promise<MailpitMessage> {
  let summaries: MailpitSummary[] = [];
  await expect(async () => {
    summaries = await messagesTo(request, to);
    expect(summaries, `expected exactly one message to ${to}`).toHaveLength(1);
  }).toPass({ timeout: 20_000 });

  const res = await request.get(`${MAILPIT_URL}/api/v1/message/${summaries[0].ID}`);
  expect(res.ok(), `Mailpit fetch message failed (${res.status()})`).toBeTruthy();
  return (await res.json()) as MailpitMessage;
}
