namespace Vibe.Office.Collaboration;

/// <summary>
/// Validates collaboration operations and batches.
/// </summary>
public static class CollabOpValidator
{
    /// <summary>
    /// Validates a single operation.
    /// </summary>
    public static bool ValidateOp(ICollabOp op, out string? error)
    {
        if (op is null)
        {
            error = "Operation is required.";
            return false;
        }

        switch (op)
        {
            case InsertTextOp insert:
                if (insert.Anchor.NodeId == Guid.Empty)
                {
                    error = "InsertText anchor NodeId is required.";
                    return false;
                }

                if (string.IsNullOrEmpty(insert.Text))
                {
                    error = "InsertText text is required.";
                    return false;
                }

                break;
            case DeleteRangeOp delete:
                if (delete.Start.NodeId == Guid.Empty || delete.End.NodeId == Guid.Empty)
                {
                    error = "DeleteRange requires valid NodeIds.";
                    return false;
                }

                break;
            case SetParagraphPropertiesOp setParagraph:
                if (setParagraph.ParagraphNodeId == Guid.Empty)
                {
                    error = "SetParagraphProperties requires a paragraph NodeId.";
                    return false;
                }

                if (setParagraph.Properties is null)
                {
                    error = "SetParagraphProperties requires properties.";
                    return false;
                }

                break;
            case SetInlinePropertiesOp setInline:
                if (setInline.InlineNodeId == Guid.Empty)
                {
                    error = "SetInlineProperties requires an inline NodeId.";
                    return false;
                }

                if (setInline.Properties is null)
                {
                    error = "SetInlineProperties requires properties.";
                    return false;
                }

                break;
            case InsertBlockOp insertBlock:
                if (insertBlock.ParentNodeId == Guid.Empty)
                {
                    error = "InsertBlock requires a parent NodeId.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(insertBlock.Position.Value))
                {
                    error = "InsertBlock requires a position token.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(insertBlock.BlockType) && (insertBlock.Payload is null || insertBlock.Payload.Length == 0))
                {
                    error = "InsertBlock requires block type or payload.";
                    return false;
                }

                break;
            case DeleteBlockOp deleteBlock:
                if (deleteBlock.ParentNodeId == Guid.Empty)
                {
                    error = "DeleteBlock requires a parent NodeId.";
                    return false;
                }

                if (deleteBlock.BlockNodeId == Guid.Empty)
                {
                    error = "DeleteBlock requires a block NodeId.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(deleteBlock.Position.Value))
                {
                    error = "DeleteBlock requires a position token.";
                    return false;
                }

                break;
            case ReplaceBlockOp replaceBlock:
                if (replaceBlock.BlockNodeId == Guid.Empty)
                {
                    error = "ReplaceBlock requires a block NodeId.";
                    return false;
                }

                if (replaceBlock.Payload is null || replaceBlock.Payload.Length == 0)
                {
                    error = "ReplaceBlock requires payload.";
                    return false;
                }

                break;
            case ReplaceDocumentResourcesOp replaceResources:
                if (replaceResources.Payload is null || replaceResources.Payload.Length == 0)
                {
                    error = "ReplaceDocumentResources requires payload.";
                    return false;
                }

                break;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Validates a batch and all contained operations.
    /// </summary>
    public static bool ValidateBatch(CollabOpBatch batch, out string? error)
    {
        if (batch.BatchId == Guid.Empty)
        {
            error = "BatchId is required.";
            return false;
        }

        if (batch.ActorId == Guid.Empty)
        {
            error = "ActorId is required.";
            return false;
        }

        if (batch.Ops.Count == 0)
        {
            error = "Batch must contain at least one op.";
            return false;
        }

        foreach (var op in batch.Ops)
        {
            if (!ValidateOp(op, out error))
            {
                return false;
            }
        }

        error = null;
        return true;
    }
}
