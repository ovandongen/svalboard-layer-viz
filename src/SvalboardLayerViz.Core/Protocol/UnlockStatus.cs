namespace SvalboardLayerViz.Core.Protocol;

/// <summary>
/// Represents the current Vial unlock state of the keyboard.
/// </summary>
/// <param name="Unlocked">True if the keyboard is already unlocked for editing.</param>
/// <param name="InProgress">True if an unlock sequence is currently in progress.</param>
/// <param name="KeysToHold">
/// Array of (row, col) pairs identifying the physical keys the user must hold
/// to complete the unlock sequence. Empty when already unlocked.
/// </param>
public record UnlockStatus(bool Unlocked, bool InProgress, (int Row, int Col)[] KeysToHold);
