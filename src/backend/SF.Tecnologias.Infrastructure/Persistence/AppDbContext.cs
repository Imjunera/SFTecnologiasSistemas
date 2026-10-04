using Microsoft.EntityFrameworkCore;
using SF.Tecnologias.Domain;

namespace SF.Tecnologias.Infrastructure.Persistence
{
    public class AppDbContext : DbContext
    {
        private readonly ITenantProvider? _tenantProvider;

        public AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider? tenantProvider = null)
            : base(options)
        {
            _tenantProvider = tenantProvider;
        }

        /// <summary>
        /// Current tenant (empresa) resolved from the authenticated user context.
        /// Null when unauthenticated (e.g., login, seeding, background jobs).
        /// </summary>
        protected int? TenantId => _tenantProvider?.GetTenantId();

        public DbSet<Empresa> Empresas { get; set; } = default!;
        public DbSet<Usuario> Usuarios { get; set; } = default!;
        public DbSet<Perfil> Perfis { get; set; } = default!;
        public DbSet<Permissao> Permissoes { get; set; } = default!;
        public DbSet<UsuarioEmpresa> UsuarioEmpresas { get; set; } = default!;
        public DbSet<Produto> Produtos { get; set; } = default!;
        public DbSet<Categoria> Categorias { get; set; } = default!;
        public DbSet<Cliente> Clientes { get; set; } = default!;
        public DbSet<Mesa> Mesas { get; set; } = default!;
        public DbSet<SessaoCaixa> SessoesCaixa { get; set; } = default!;
        public DbSet<Venda> Vendas { get; set; } = default!;
        public DbSet<VendaItem> VendaItens { get; set; } = default!;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.CriadoEm = now;
                }
                if (entry.State == EntityState.Modified)
                {
                    entry.Entity.AtualizadoEm = now;
                }
            }
            return base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Empresa
            modelBuilder.Entity<Empresa>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Codigo).IsRequired().HasMaxLength(50);
                e.Property(x => x.Nome).IsRequired().HasMaxLength(200);
                e.Property(x => x.Ativo).IsRequired();
                e.HasIndex(x => x.Codigo).IsUnique();
                e.HasIndex(x => x.Nome);
                e.ToTable(t => t.HasCheckConstraint("CK_Empresas_Codigo_NaoVazio", "\"Codigo\" <> ''"));
                e.ToTable(t => t.HasCheckConstraint("CK_Empresas_Nome_NaoVazio", "\"Nome\" <> ''"));
            });

            // Usuario
            modelBuilder.Entity<Usuario>(u =>
            {
                u.HasKey(x => x.Id);
                u.Property(x => x.Nome).IsRequired().HasMaxLength(200);
                u.Property(x => x.Email).IsRequired().HasMaxLength(200);
                u.HasIndex(x => x.Email).IsUnique();
                u.Property(x => x.SenhaHash).IsRequired().HasMaxLength(255); // bcrypt hash
                u.Property(x => x.Ativo).IsRequired();
            });

            // Perfil
            modelBuilder.Entity<Perfil>(p =>
            {
                p.HasKey(x => x.Id);
                p.Property(x => x.Nome).IsRequired().HasMaxLength(100);
                p.Property(x => x.Descricao).HasMaxLength(500);
                p.Property(x => x.Ativo).IsRequired();
                p.HasIndex(x => x.Nome).IsUnique();
                // Perfil <-> Permissao: many-to-many (permissions are a global catalog)
                // The join table "PerfilPermissoes" gets a composite PK (PerfilId, PermissaoId) by convention.
                p.HasMany(x => x.Permissoes)
                    .WithMany()
                    .UsingEntity("PerfilPermissoes");
            });

            // Permissao
            modelBuilder.Entity<Permissao>(p =>
            {
                p.HasKey(x => x.Id);
                p.Property(x => x.Nome).IsRequired().HasMaxLength(100);
                p.Property(x => x.Descricao).HasMaxLength(500);
                p.HasIndex(x => x.Nome).IsUnique();
            });

            // UsuarioEmpresa
            modelBuilder.Entity<UsuarioEmpresa>(ue =>
            {
                ue.HasKey(x => x.Id);
                ue.HasOne(x => x.Usuario)
                  .WithMany(u => u.UsuarioEmpresas)
                  .HasForeignKey(x => x.UsuarioId)
                  .OnDelete(DeleteBehavior.Restrict);
                ue.HasOne(x => x.Empresa)
                  .WithMany(e => e.UsuarioEmpresas)
                  .HasForeignKey(x => x.EmpresaId)
                  .OnDelete(DeleteBehavior.Restrict);
                ue.HasOne(x => x.Perfil)
                  .WithMany(p => p.UsuarioEmpresas)
                  .HasForeignKey(x => x.PerfilId)
                  .OnDelete(DeleteBehavior.Restrict);
                ue.HasIndex(x => new { x.UsuarioId, x.EmpresaId }).IsUnique();
                ue.Property(x => x.Ativo).IsRequired();
            });

            // Produto
            modelBuilder.Entity<Produto>(p =>
            {
                p.HasKey(x => x.Id);
                p.Property(x => x.Nome).IsRequired().HasMaxLength(200);
                p.Property(x => x.Descricao).HasMaxLength(500);
                p.Property(x => x.Codigo).IsRequired().HasMaxLength(50);
                p.Property(x => x.PrecoVenda).HasColumnType("numeric(18,2)").IsRequired();
                p.Property(x => x.PrecoCusto).HasColumnType("numeric(18,2)");
                p.Property(x => x.Ativo).IsRequired();
                p.HasOne(x => x.Empresa)
                 .WithMany()
                 .HasForeignKey(x => x.EmpresaId)
                 .OnDelete(DeleteBehavior.Restrict);
                p.HasOne(x => x.Categoria)
                 .WithMany()
                 .HasForeignKey(x => x.CategoriaId)
                 .OnDelete(DeleteBehavior.SetNull);
                p.HasIndex(x => new { x.EmpresaId, x.Codigo }).IsUnique();
                p.HasIndex(x => new { x.EmpresaId, x.Nome });
                p.ToTable(t => t.HasCheckConstraint("CK_Produtos_PrecoVenda_Positivo", "\"PrecoVenda\" > 0"));
            });

            // Categoria
            modelBuilder.Entity<Categoria>(c =>
            {
                c.HasKey(x => x.Id);
                c.Property(x => x.Nome).IsRequired().HasMaxLength(100);
                c.Property(x => x.Descricao).HasMaxLength(500);
                c.Property(x => x.Ordem).HasDefaultValue(0);
                c.Property(x => x.Ativo).IsRequired();
                c.HasOne(x => x.Empresa)
                 .WithMany()
                 .HasForeignKey(x => x.EmpresaId)
                 .OnDelete(DeleteBehavior.Restrict);
                c.HasIndex(x => new { x.EmpresaId, x.Nome }).IsUnique();
            });

            // Cliente
            modelBuilder.Entity<Cliente>(c =>
            {
                c.HasKey(x => x.Id);
                c.Property(x => x.Nome).IsRequired().HasMaxLength(200);
                c.Property(x => x.Telefone).HasMaxLength(20);
                c.Property(x => x.Cpf).HasMaxLength(14);
                c.Property(x => x.Email).HasMaxLength(200);
                c.Property(x => x.Endereco).HasMaxLength(500);
                c.Property(x => x.Observacoes).HasMaxLength(500);
                c.Property(x => x.Ativo).IsRequired();
                c.HasOne(x => x.Empresa)
                 .WithMany()
                 .HasForeignKey(x => x.EmpresaId)
                 .OnDelete(DeleteBehavior.Restrict);
                c.HasIndex(x => new { x.EmpresaId, x.Nome });
            });

            // Mesa
            modelBuilder.Entity<Mesa>(m =>
            {
                m.HasKey(x => x.Id);
                m.Property(x => x.Numero).IsRequired();
                m.Property(x => x.Descricao).HasMaxLength(100);
                m.Property(x => x.Capacidade).HasDefaultValue(4);
                m.Property(x => x.Status).IsRequired();
                m.Property(x => x.Ativo).IsRequired();
                m.HasOne(x => x.Empresa)
                 .WithMany()
                 .HasForeignKey(x => x.EmpresaId)
                 .OnDelete(DeleteBehavior.Restrict);
                m.HasIndex(x => new { x.EmpresaId, x.Numero }).IsUnique();
            });

            // SessaoCaixa
            modelBuilder.Entity<SessaoCaixa>(sc =>
            {
                sc.HasKey(x => x.Id);
                sc.Property(x => x.ValorInicial).HasColumnType("numeric(18,2)").IsRequired();
                sc.Property(x => x.ValorFechamento).HasColumnType("numeric(18,2)");
                sc.Property(x => x.Status).IsRequired();
                sc.HasOne(x => x.Empresa)
                 .WithMany()
                 .HasForeignKey(x => x.EmpresaId)
                 .OnDelete(DeleteBehavior.Restrict);
                sc.HasOne(x => x.Usuario)
                 .WithMany()
                 .HasForeignKey(x => x.UsuarioId)
                 .OnDelete(DeleteBehavior.Restrict);
                sc.HasOne(x => x.Mesa)
                 .WithMany()
                 .HasForeignKey(x => x.MesaId)
                 .OnDelete(DeleteBehavior.SetNull);
                sc.HasIndex(x => new { x.EmpresaId, x.Status });
            });

            // Venda
            modelBuilder.Entity<Venda>(v =>
            {
                v.HasKey(x => x.Id);
                v.Property(x => x.FormaPagamento).IsRequired().HasMaxLength(20);
                v.Property(x => x.ValorTotal).HasColumnType("numeric(18,2)").IsRequired();
                v.Property(x => x.Status).IsRequired();
                v.HasOne(x => x.Empresa)
                 .WithMany()
                 .HasForeignKey(x => x.EmpresaId)
                 .OnDelete(DeleteBehavior.Restrict);
                v.HasOne(x => x.Usuario)
                 .WithMany()
                 .HasForeignKey(x => x.UsuarioId)
                 .OnDelete(DeleteBehavior.Restrict);
                v.HasOne(x => x.SessaoCaixa)
                 .WithMany()
                 .HasForeignKey(x => x.SessaoCaixaId)
                 .OnDelete(DeleteBehavior.SetNull);
                v.HasOne(x => x.Cliente)
                 .WithMany()
                 .HasForeignKey(x => x.ClienteId)
                 .OnDelete(DeleteBehavior.SetNull);
                v.HasMany(x => x.Itens)
                 .WithOne(i => i.Venda)
                 .HasForeignKey(i => i.VendaId)
                 .OnDelete(DeleteBehavior.Cascade);
                v.HasIndex(x => new { x.EmpresaId, x.DataVenda });
                v.HasIndex(x => new { x.EmpresaId, x.SessaoCaixaId });
                v.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_Vendas_ValorTotal_Negativo", "\"ValorTotal\" >= 0");
                    t.HasCheckConstraint("CK_Vendas_FormaPagamento_NaoVazia", "\"FormaPagamento\" <> ''");
                });
            });

            // VendaItem
            modelBuilder.Entity<VendaItem>(vi =>
            {
                vi.HasKey(x => x.Id);
                vi.Property(x => x.ProdutoNome).IsRequired().HasMaxLength(200);
                vi.Property(x => x.Quantidade).IsRequired();
                vi.Property(x => x.PrecoUnitario).HasColumnType("numeric(18,2)").IsRequired();
                vi.Property(x => x.Subtotal).HasColumnType("numeric(18,2)").IsRequired();
                vi.HasOne(x => x.Empresa)
                 .WithMany()
                 .HasForeignKey(x => x.EmpresaId)
                 .OnDelete(DeleteBehavior.Restrict);
                vi.HasOne(x => x.Produto)
                 .WithMany()
                 .HasForeignKey(x => x.ProdutoId)
                 .OnDelete(DeleteBehavior.SetNull);
                vi.HasIndex(x => x.VendaId);
                vi.ToTable(t =>
                {
                    t.HasCheckConstraint("CK_VendaItens_Quantidade_Positiva", "\"Quantidade\" > 0");
                    t.HasCheckConstraint("CK_VendaItens_PrecoUnitario_NaoNegativo", "\"PrecoUnitario\" >= 0");
                    t.HasCheckConstraint("CK_VendaItens_Subtotal_NaoNegativo", "\"Subtotal\" >= 0");
                });
            });

            ApplyTenantQueryFilters(modelBuilder);

            base.OnModelCreating(modelBuilder);
        }

        /// <summary>
        /// Automatically applies a global query filter to every entity implementing ITenantEntity.
        /// When TenantId is null (unauthenticated context), no filtering occurs,
        /// allowing login and seeding to work.
        /// </summary>
        private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (!typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
                    continue;

                var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                var tenantIdProperty = System.Linq.Expressions.Expression.Property(
                    System.Linq.Expressions.Expression.Constant(this),
                    nameof(TenantId));
                var empresaIdProperty = System.Linq.Expressions.Expression.Property(
                    parameter,
                    nameof(ITenantEntity.EmpresaId));
                var body = System.Linq.Expressions.Expression.OrElse(
                    System.Linq.Expressions.Expression.Equal(
                        tenantIdProperty,
                        System.Linq.Expressions.Expression.Constant(null, typeof(int?))),
                    System.Linq.Expressions.Expression.Equal(
                        System.Linq.Expressions.Expression.Convert(empresaIdProperty, typeof(int?)),
                        tenantIdProperty));
                var lambda = System.Linq.Expressions.Expression.Lambda(body, parameter);
                entityType.SetQueryFilter(lambda);
            }
        }
    }
}
