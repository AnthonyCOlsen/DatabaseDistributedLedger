using System.Buffers.Binary;

namespace UserDimensions.Ledger;

public sealed record RevocationConsent(string LedgerId, Guid BlockId, long Timestamp, byte[] OuterSigner,
    byte[] Target, byte[] Countersigner, byte[] Signature)
{
    public byte[] Intent => Signing.RevocationIntent(LedgerId,BlockId,Timestamp,OuterSigner,Target,Countersigner);
    public byte[] Encode() => Intent.Concat(Signature).ToArray();

    public static RevocationConsent Parse(byte[] package)
    {
        Errors.Require(package.Length==229 && package.AsSpan(0,13).SequenceEqual("DDL-REVOKE-v3"u8),"BAD_EVENT",2,"Expected exact 229-byte v3 consent package.");
        var consent=new RevocationConsent(Binary.Hex(package.AsSpan(13,32)),new Guid(package.AsSpan(45,16),true),
            BinaryPrimitives.ReadInt64BigEndian(package.AsSpan(61,8)),package[69..101],package[101..133],package[133..165],package[165..]);
        Errors.Require(consent.BlockId!=Guid.Empty && !consent.OuterSigner.AsSpan().SequenceEqual(consent.Countersigner) &&
            (consent.Target.AsSpan().SequenceEqual(consent.OuterSigner) || consent.Target.AsSpan().SequenceEqual(consent.Countersigner)),"BAD_EVENT",2,"Invalid consent identity or roles.");
        Errors.Require(Signing.Verify(consent.Countersigner,consent.Intent,consent.Signature),"BAD_SIGNATURE",2,"Invalid consent signature.");
        return consent;
    }

    public static long CheckInterval(long blockTime,long consentTime)
    {
        long delta;
        try { delta=checked(blockTime-consentTime); }
        catch (OverflowException) { throw new LedgerException("CONSENT_EXPIRED",2,"Consent timestamp interval overflow."); }
        Errors.Require(delta>=0 && delta<=900000,"CONSENT_EXPIRED",2,"Consent must precede the final signed timestamp by at most 15 minutes.");
        return delta;
    }
}
