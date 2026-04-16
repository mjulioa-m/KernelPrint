type InvoiceItem = {
  id: string
  description: string
  qty: number
  unitPrice: number
}

const items: InvoiceItem[] = Array.from({ length: 35 }, (_, index) => {
  const line = index + 1
  return {
    id: `ITM-${line.toString().padStart(3, '0')}`,
    description: `Premium service bundle #${line} with implementation and support coverage.`,
    qty: 1 + (line % 4),
    unitPrice: 79 + line * 3.25,
  }
})

const subtotal = items.reduce((acc, item) => acc + item.qty * item.unitPrice, 0)
const tax = subtotal * 0.13
const total = subtotal + tax

function money(value: number): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value)
}

function App() {
  return (
    <div className="page">
      <div className="toolbar no-print">
        <button type="button" className="print-button" onClick={() => window.print()}>
          Imprimir factura
        </button>
        <span>Prueba visual en navegador para comparar con PDF.</span>
      </div>

      <article className="invoice">
        <header className="hero">
          <div>
            <p className="eyebrow">KernelPrint Billing</p>
            <h1>Invoice INV-2026-0415</h1>
            <p className="hero-text">Cloud rendering services and enterprise support package.</p>
          </div>
          <div className="hero-right">
            <p><strong>Issue date:</strong> 2026-04-15</p>
            <p><strong>Due date:</strong> 2026-04-30</p>
            <p><strong>Status:</strong> Pending payment</p>
          </div>
        </header>

        <section className="cards">
          <div className="card">
            <p className="card-label">Billed To</p>
            <p className="card-title">ACME Corporation</p>
            <p>accounts@acme.com</p>
            <p>742 Evergreen Ave, Springfield</p>
          </div>
          <div className="card">
            <p className="card-label">From</p>
            <p className="card-title">KernelPrint LLC</p>
            <p>billing@kernelprint.io</p>
            <p>128 Market St, Austin, TX</p>
          </div>
          <div className="card">
            <p className="card-label">Summary</p>
            <p className="card-title">{items.length} line items</p>
            <p>Plan: Enterprise Annual</p>
            <p>Support SLA: 24/7 Premium</p>
          </div>
        </section>

        <table className="items">
          <thead>
            <tr>
              <th>Item</th>
              <th>Description</th>
              <th className="text-right">Qty</th>
              <th className="text-right">Unit Price</th>
              <th className="text-right">Line Total</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.id}>
                <td>{item.id}</td>
                <td>{item.description}</td>
                <td className="text-right">{item.qty}</td>
                <td className="text-right">{money(item.unitPrice)}</td>
                <td className="text-right">{money(item.qty * item.unitPrice)}</td>
              </tr>
            ))}
          </tbody>
        </table>

        <section className="totals">
          <div />
          <div className="totals-box">
            <p><span>Subtotal</span><strong>{money(subtotal)}</strong></p>
            <p><span>Tax (13%)</span><strong>{money(tax)}</strong></p>
            <p className="grand"><span>Total</span><strong>{money(total)}</strong></p>
          </div>
        </section>
      </article>
    </div>
  )
}

export default App
