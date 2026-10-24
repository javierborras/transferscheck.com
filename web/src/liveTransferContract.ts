// Mirrors contracts/LiveTransferArrived.json — keep fields in sync with the API publisher.
export const liveTransferMessageType = "LiveTransferArrived";
export const liveTransferFields = ["id", "payerName", "amount", "occurredAt", "status", "statusLabel"] as const;

export type LiveTransferArrived = {
  messageType: typeof liveTransferMessageType;
  id: string;
  payerName: string;
  amount: number;
  occurredAt: string;
  status: string;
  statusLabel: string;
};
