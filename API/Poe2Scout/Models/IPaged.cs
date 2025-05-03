namespace NinjaPricer.API.Poe2Scout.Models;

public interface IPaged<T>
{
    public T[] items { get; }
    int total { get; }
    int pages { get; }
    int currentPage { get; }
}