using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BlockchainObserver.Database.Entities
{
    public class XpubAddressIndex
    {
        [Key]
        [Required]
        public int Id { get; set; }

        [Required]
        [StringLength(112)]
        public string Xpub { get; set; }

        [Required]
        public int Index { get; set; }
    }
}
