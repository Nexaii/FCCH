using Lumina.Data;
using Lumina.Excel;

namespace FCCH.GameData
{
    [Sheet("CompanyCraftSupplyItem")]
    public readonly struct CompanyCraftSupplyItem(ExcelPage page, uint offset, uint row) : IExcelRow<CompanyCraftSupplyItem>
    {
        public uint RowId => row;
        public uint SubRowId => 0;
        public uint RowOffset => offset;
        public ExcelPage ExcelPage => page;

        public uint Item => page.ReadUInt32(offset);

        static CompanyCraftSupplyItem IExcelRow<CompanyCraftSupplyItem>.Create(ExcelPage page, uint offset, uint row) => new(page, offset, row);
    }

    [Sheet("CompanyCraftSequence")]
    public readonly struct CompanyCraftSequence(ExcelPage page, uint offset, uint row) : IExcelRow<CompanyCraftSequence>
    {
        public uint RowId => row;
        public uint SubRowId => 0;
        public uint RowOffset => offset;
        public ExcelPage ExcelPage => page;

        public uint ResultItem => page.ReadUInt32(offset + 4);
        public int Category => page.ReadInt32(offset + 8);
        public uint CompanyCraftDraftCategory => page.ReadUInt32(offset + 12);
        public uint CompanyCraftType => page.ReadUInt32(offset + 16);
        public uint CompanyCraftDraft => page.ReadUInt32(offset + 20);

        public ushort[] CompanyCraftPart
        {
            get
            {
                var parts = new ushort[8];
                for (int i = 0; i < 8; i++)
                {
                    parts[i] = page.ReadUInt16(offset + 24 + (uint)(i * 2));
                }
                return parts;
            }
        }

        static CompanyCraftSequence IExcelRow<CompanyCraftSequence>.Create(ExcelPage page, uint offset, uint row) => new(page, offset, row);
    }

    [Sheet("CompanyCraftPart")]
    public readonly struct CompanyCraftPart(ExcelPage page, uint offset, uint row) : IExcelRow<CompanyCraftPart>
    {
        public uint RowId => row;
        public uint SubRowId => 0;
        public uint RowOffset => offset;
        public ExcelPage ExcelPage => page;

        public ushort[] CompanyCraftProcess
        {
            get
            {
                var processes = new ushort[3];
                for (int i = 0; i < 3; i++)
                {
                    processes[i] = page.ReadUInt16(offset + (uint)(i * 2));
                }
                return processes;
            }
        }

        public uint CompanyCraftType => page.ReadUInt32(offset + 16);

        static CompanyCraftPart IExcelRow<CompanyCraftPart>.Create(ExcelPage page, uint offset, uint row) => new(page, offset, row);
    }

    [Sheet("CompanyCraftProcess")]
    public readonly struct CompanyCraftProcess(ExcelPage page, uint offset, uint row) : IExcelRow<CompanyCraftProcess>
    {
        public uint RowId => row;
        public uint SubRowId => 0;
        public uint RowOffset => offset;
        public ExcelPage ExcelPage => page;

        public ushort[] SupplyItem
        {
            get
            {
                var items = new ushort[12];
                for (int i = 0; i < 12; i++)
                {
                    items[i] = page.ReadUInt16(offset + (uint)(i * 2));
                }
                return items;
            }
        }

        public ushort[] SetQuantity
        {
            get
            {
                var quantities = new ushort[12];
                for (int i = 0; i < 12; i++)
                {
                    quantities[i] = page.ReadUInt16(offset + 24 + (uint)(i * 2));
                }
                return quantities;
            }
        }

        public ushort[] SetsRequired
        {
            get
            {
                var required = new ushort[12];
                for (int i = 0; i < 12; i++)
                {
                    required[i] = page.ReadUInt16(offset + 48 + (uint)(i * 2));
                }
                return required;
            }
        }

        static CompanyCraftProcess IExcelRow<CompanyCraftProcess>.Create(ExcelPage page, uint offset, uint row) => new(page, offset, row);
    }

    [Sheet("CompanyCraftType")]
    public readonly struct CompanyCraftType(ExcelPage page, uint offset, uint row) : IExcelRow<CompanyCraftType>
    {
        public uint RowId => row;
        public uint SubRowId => 0;
        public uint RowOffset => offset;
        public ExcelPage ExcelPage => page;

        public string Name => page.ReadString(offset, offset).ToString();

        static CompanyCraftType IExcelRow<CompanyCraftType>.Create(ExcelPage page, uint offset, uint row) => new(page, offset, row);
    }

    [Sheet("CompanyCraftDraftCategory")]
    public readonly struct CompanyCraftDraftCategory(ExcelPage page, uint offset, uint row) : IExcelRow<CompanyCraftDraftCategory>
    {
        public uint RowId => row;
        public uint SubRowId => 0;
        public uint RowOffset => offset;
        public ExcelPage ExcelPage => page;

        public string Name => page.ReadString(offset, offset).ToString();

        public uint[] CompanyCraftType
        {
            get
            {
                var types = new uint[10];
                for (int i = 0; i < 10; i++)
                {
                    types[i] = page.ReadUInt32(offset + 4 + (uint)(i * 4));
                }
                return types;
            }
        }

        static CompanyCraftDraftCategory IExcelRow<CompanyCraftDraftCategory>.Create(ExcelPage page, uint offset, uint row) => new(page, offset, row);
    }
}
