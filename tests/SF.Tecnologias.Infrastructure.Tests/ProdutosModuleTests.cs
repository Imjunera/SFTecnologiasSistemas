using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SF.Tecnologias.Application.DTOs;
using SF.Tecnologias.Application.Services;
using SF.Tecnologias.Domain;
using Xunit;

namespace SF.Tecnologias.Infrastructure.Tests;

public class ProdutosModuleTests : DatabaseTestBase
{
    private async Task<(Empresa empresaA, Empresa empresaB)> SeedEmpresasAsync()
    {
        await using var context = CreateContext();
        var empresaA = new Empresa { Codigo = "EMP_A", Nome = "Empresa A", Ativo = true };
        var empresaB = new Empresa { Codigo = "EMP_B", Nome = "Empresa B", Ativo = true };
        context.Empresas.AddRange(empresaA, empresaB);
        await context.SaveChangesAsync();
        return (empresaA, empresaB);
    }

    [Fact]
    public async Task CadastroProduto_ComDadosValidos_PersisteComSucesso()
    {
        var (empresaA, _) = await SeedEmpresasAsync();
        var fakeProvider = new FakeTenantProvider(empresaA.Id);

        await using var context = CreateContext(fakeProvider);
        var service = new ProdutoService(context, fakeProvider);

        var request = new CriarProdutoRequest
        {
            Nome = "Coca-Cola 350ml",
            Descricao = "Refrigerante lata",
            Codigo = "789123456",
            PrecoVenda = 6.50m,
            PrecoCusto = 3.20m
        };

        var criado = await service.CriarAsync(request);

        Assert.True(criado.Id > 0);
        Assert.Equal(empresaA.Id, criado.EmpresaId);
        Assert.Equal("Coca-Cola 350ml", criado.Nome);
        Assert.Equal(6.50m, criado.PrecoVenda);
        Assert.True(criado.Ativo);

        // Verifica persistência direta no banco
        await using var verifyContext = CreateContext();
        var persisted = await verifyContext.Produtos.FindAsync(criado.Id);
        Assert.NotNull(persisted);
        Assert.Equal("789123456", persisted.Codigo);
    }

    [Fact]
    public async Task CadastroProduto_CodigoDuplicadoNaMesmaEmpresa_LancaExcecao()
    {
        var (empresaA, _) = await SeedEmpresasAsync();
        var fakeProvider = new FakeTenantProvider(empresaA.Id);

        await using var context = CreateContext(fakeProvider);
        var service = new ProdutoService(context, fakeProvider);

        await service.CriarAsync(new CriarProdutoRequest
        {
            Nome = "Agua Mineral",
            Codigo = "PROD001",
            PrecoVenda = 3.50m
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await service.CriarAsync(new CriarProdutoRequest
            {
                Nome = "Agua com Gas",
                Codigo = "PROD001",
                PrecoVenda = 4.00m
            });
        });
    }

    [Fact]
    public async Task CadastroProduto_MesmoCodigoEmEmpresasDiferentes_Permitido()
    {
        var (empresaA, empresaB) = await SeedEmpresasAsync();

        var providerA = new FakeTenantProvider(empresaA.Id);
        await using var contextA = CreateContext(providerA);
        var serviceA = new ProdutoService(contextA, providerA);

        var prodA = await serviceA.CriarAsync(new CriarProdutoRequest
        {
            Nome = "Item Comum",
            Codigo = "COMUM01",
            PrecoVenda = 10.00m
        });

        var providerB = new FakeTenantProvider(empresaB.Id);
        await using var contextB = CreateContext(providerB);
        var serviceB = new ProdutoService(contextB, providerB);

        var prodB = await serviceB.CriarAsync(new CriarProdutoRequest
        {
            Nome = "Outro Item Comum",
            Codigo = "COMUM01",
            PrecoVenda = 12.00m
        });

        Assert.True(prodA.Id > 0);
        Assert.True(prodB.Id > 0);
        Assert.NotEqual(prodA.Id, prodB.Id);
    }

    [Fact]
    public async Task ValidaPrecoVenda_MenorOuIgualAZero_Rejeitado()
    {
        var (empresaA, _) = await SeedEmpresasAsync();
        var fakeProvider = new FakeTenantProvider(empresaA.Id);

        await using var context = CreateContext(fakeProvider);
        var service = new ProdutoService(context, fakeProvider);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await service.CriarAsync(new CriarProdutoRequest
            {
                Nome = "Produto Invalido",
                Codigo = "INV01",
                PrecoVenda = 0m
            });
        });

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await service.CriarAsync(new CriarProdutoRequest
            {
                Nome = "Produto Negativo",
                Codigo = "INV02",
                PrecoVenda = -5.00m
            });
        });
    }

    [Fact]
    public async Task MultiTenant_Isolamento_EmpresaNaoVisualizaProdutosDeOutra()
    {
        var (empresaA, empresaB) = await SeedEmpresasAsync();

        var providerA = new FakeTenantProvider(empresaA.Id);
        await using (var contextA = CreateContext(providerA))
        {
            var serviceA = new ProdutoService(contextA, providerA);
            await serviceA.CriarAsync(new CriarProdutoRequest
            {
                Nome = "Exclusivo Empresa A",
                Codigo = "EXCLA",
                PrecoVenda = 50.00m
            });
        }

        var providerB = new FakeTenantProvider(empresaB.Id);
        await using (var contextB = CreateContext(providerB))
        {
            var serviceB = new ProdutoService(contextB, providerB);
            var produtosB = (await serviceB.ObterTodosAsync()).ToList();

            Assert.Empty(produtosB);
        }

        await using (var contextA2 = CreateContext(providerA))
        {
            var serviceA2 = new ProdutoService(contextA2, providerA);
            var produtosA = (await serviceA2.ObterTodosAsync()).ToList();

            Assert.Single(produtosA);
            Assert.Equal("Exclusivo Empresa A", produtosA[0].Nome);
        }
    }

    [Fact]
    public async Task EdicaoEInativacao_AtualizaCorretamente()
    {
        var (empresaA, _) = await SeedEmpresasAsync();
        var fakeProvider = new FakeTenantProvider(empresaA.Id);

        await using var context = CreateContext(fakeProvider);
        var service = new ProdutoService(context, fakeProvider);

        var criado = await service.CriarAsync(new CriarProdutoRequest
        {
            Nome = "Coxinha",
            Codigo = "LANCHE01",
            PrecoVenda = 8.00m
        });

        // Edição
        var atualizado = await service.AtualizarAsync(criado.Id, new AtualizarProdutoRequest
        {
            Nome = "Coxinha Especial",
            Codigo = "LANCHE01",
            PrecoVenda = 9.50m,
            Ativo = true
        });

        Assert.Equal("Coxinha Especial", atualizado.Nome);
        Assert.Equal(9.50m, atualizado.PrecoVenda);

        // Inativação
        var inativado = await service.InativarAsync(criado.Id);
        Assert.True(inativado);

        var buscaAtivos = (await service.ObterTodosAsync(apenasAtivos: true)).ToList();
        Assert.Empty(buscaAtivos);

        var buscaTodos = (await service.ObterTodosAsync(apenasAtivos: false)).ToList();
        Assert.Single(buscaTodos);
        Assert.False(buscaTodos[0].Ativo);
    }
}

