/// Produced by the shop PC's own C# code (`Sync/DataKey.cs`, `RowCipher`), not by the app: if the phone opens this, the formats match.
const shopRecoveryCode = 'DLNE-EURX-WYBS-ECX5-TSMP-KPIZ-WQPA-3Y4Y-JS2J-4UYN-KN4P-LPPW-GIC7-JAXS';
const shopKeyBase64 = 'GtpCUje2AyIK/ZyY9T0ZtB4N45hMtJ5TDVN49b32MgU=';

/// A different shop's valid recovery code.
const otherShopRecoveryCode = 'QLYC-6L2T-PL4O-5SE2-YRAX-UYV7-3PGB-PA5L-V74N-DZVX-4S44-TYMN-DA7E-GJ4O';

const vectorTable = 'Products';
const vectorId = 42;
const vectorPayload =
    'e1.BgBsXvO4FAyzK9hrh9ciQWRR6vOI40YoDt480dr9PopGgazexKwQoYB/h2XAzCLgnqYz/A6EupipyDvToJXJdBQEjJPLvmVkw8PhaVEmizcC750+vsiGTK02g81QJyAhDpgzt9mt4HFpTtSu6ixvO8SXIcFDE5qpyJD8JT7dhXt+q5HbEX3d4w0PXS8YIjb9WfZSRREhZkGPSMYloLWjx1NGTKxMAYc=';

/// What the vector row decrypts to (Arabic + English name, a decimal, an integer, a null and a boolean).
const vectorName = 'حليب طازج - Fresh Milk';
