namespace EqoaLoadClient.Core.Bot;
/// New -> Joining (join sent, awaiting the server's reply) -> InWorld (movement starts) -> Closed.
public enum BotState { New, Joining, InWorld, Closed }
