using System;
using System.Collections.Generic;
using System.Text;

namespace AsynCUDA13.Shared.MediaDtos
{
    public interface IMediaInfo
    {
        Guid Id { get; set; }
        string Name { get; set; }
        string? Pointer { get; set; }
    }

    public interface IMediaData
    {

    }
}
